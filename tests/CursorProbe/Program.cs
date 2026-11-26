using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.OpenHarmony;
using Avalonia.Platform;
using Avalonia.Threading;

var dll = NativeLibrary.Load(Path.GetFullPath(args[0]));
NativeLibrary.SetDllImportResolver(typeof(OpenHarmonyHost).Assembly, (name, _, _) =>
    name is "libohinput.so" or "libpixelmap.so" ? dll : 0);
var read = Marshal.GetDelegateForFunctionPointer<ReadSnapshot>(NativeLibrary.GetExport(dll, "ReadCursorProbe"));
var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var host = new OpenHarmonyHost(() => AppBuilder.Configure<CursorApp>(),
    (_, _, _, _, _) => ready.TrySetResult(), Console.Error.WriteLine, () => 17);
host.UpdateSurface(1, 300, 180, 1);
await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));

await MoveAndCheck(30, 30, values =>
{
    Check(values is { Window: 17, Style: -100, Width: 32, Height: 24, X: 7, Y: 5, FollowSystem: 0 },
        "The cursor must preserve the window, dimensions and physical hotspot.");
    Check(values is { Pixel: 0xff0102c0, NonZeroPixels: 1, Format: 3, SourceFormat: 3, Alpha: 2 },
        "The source BGRA bitmap must become packed, premultiplied RGBA.");
});
await MoveAndCheck(30, 75, values =>
    Check(values is { Window: 17, Style: -100, NonZeroPixels: 0 },
        "The hidden cursor must be fully transparent in the same window."));
await MoveAndCheck(30, 125, values =>
    Check(values is { Window: 17, Style: 26 }, "Moving to another control must restore a standard cursor."));
await MoveAndCheck(30, 165, values =>
    Check(values is { Window: 17, Style: 0 }, "A bitmap with an invalid hotspot must fall back to arrow."));
await MoveAndCheck(30, 30, values =>
    Check(values is { Window: 17, Style: -100, Width: 32, Height: 24 },
        "Re-entering the bitmap cursor control must reuse its native bitmap."));
await Dispatcher.UIThread.InvokeAsync(() =>
{
    CursorApp.BitmapCursor!.Dispose();
    CursorApp.HiddenCursor!.Dispose();
    CursorApp.BitmapCursor.Dispose();
    Check(Marshal.PtrToStructure<CursorSnapshot>(read()).Released == 2,
        "Each cursor must release its pixel map once.");
});
Console.WriteLine("Bitmap, transparent and standard cursor transitions and resource disposal passed.");

async Task MoveAndCheck(double x, double y, Action<CursorSnapshot> verify)
{
    host.HardwarePointer(1, 0, 0, x, y, 0, 0, 0);
    await Dispatcher.UIThread.InvokeAsync(() => verify(Marshal.PtrToStructure<CursorSnapshot>(read())),
        DispatcherPriority.Background);
}

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate nint ReadSnapshot();

[StructLayout(LayoutKind.Sequential)]
internal struct CursorSnapshot
{
    public int Window, Style, Width, Height, X, Y, FollowSystem, Format, SourceFormat, Alpha, NonZeroPixels, Released;
    public uint Pixel;
}

public sealed class CursorApp : Application
{
    internal static Cursor? BitmapCursor { get; private set; }
    internal static Cursor? HiddenCursor { get; private set; }

    public override void OnFrameworkInitializationCompleted()
    {
        using var bitmap = new WriteableBitmap(new PixelSize(32, 24), new Vector(192, 192),
            PixelFormats.Bgra8888, AlphaFormat.Premul);
        using (var pixels = bitmap.Lock())
        {
            var clear = new byte[pixels.RowBytes * pixels.Size.Height];
            Marshal.Copy(clear, 0, pixels.Address, clear.Length);
            Marshal.WriteInt32(pixels.Address, unchecked((int)0xffc00201));
        }
        BitmapCursor = new Cursor(bitmap, new PixelPoint(7, 5));
        HiddenCursor = new Cursor(StandardCursorType.None);
        var panel = new StackPanel();
        panel.Children.Add(CreateZone(BitmapCursor));
        panel.Children.Add(CreateZone(HiddenCursor));
        panel.Children.Add(CreateZone(new Cursor(StandardCursorType.Ibeam)));
        panel.Children.Add(CreateZone(new Cursor(bitmap, new PixelPoint(32, 5))));
        ((ISingleViewApplicationLifetime)ApplicationLifetime!).MainView = panel;
        base.OnFrameworkInitializationCompleted();
    }

    private static Border CreateZone(Cursor cursor) => new() { Height = 45, Background = Brushes.Blue, Cursor = cursor };
}
