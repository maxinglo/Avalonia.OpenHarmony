using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.OpenHarmony;
using Avalonia.Platform;
using Avalonia.Threading;

var library = NativeLibrary.Load(Path.GetFullPath(args[0]));
NativeLibrary.SetDllImportResolver(typeof(OpenHarmonyHost).Assembly, (name, _, _) =>
    name is "libpasteboard.so" or "libudmf.so" or "libpixelmap.so" ? library : 0);
var nativeMode = Marshal.GetDelegateForFunctionPointer<SetProbeMode>(NativeLibrary.GetExport(library, "SetProbeMode"));
var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var host = new OpenHarmonyHost(() => AppBuilder.Configure<ClipboardApp>(),
    (_, _, _, _, _) => ready.TrySetResult(), Console.Error.WriteLine);
host.UpdateSurface(1, 300, 180, 1);
await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
await Dispatcher.UIThread.InvokeAsync(async () =>
{
    var clipboard = TopLevel.GetTopLevel(ClipboardApp.TextBox)?.Clipboard ?? throw new InvalidOperationException("No clipboard.");
    var html = DataFormat.CreateStringPlatformFormat("text/html");
    var customText = DataFormat.CreateStringApplicationFormat("probe.text");
    var customBytes = DataFormat.CreateBytesApplicationFormat("probe.bytes");
    var inProcess = DataFormat.CreateInProcessFormat<object>("probe.local");
    var bitmapSize = new PixelSize(3, 2);
    byte[] expectedPixels = [160, 56, 24, 255, 64, 32, 8, 128, 0, 0, 0, 0,
        0, 0, 80, 255, 0, 0, 0, 0, 32, 80, 248, 255];
    using var source = MakeBitmap(expectedPixels, bitmapSize);
    var item = DataTransferItem.CreateText("First — 中文 🎧");
    item.Set(html, "<b>First</b>");
    item.Set(DataFormat.Bitmap, source);
    item.Set(customText, "α\0🎧");
    item.Set(customBytes, []);
    item.Set(inProcess, new object());
    var transfer = new DataTransfer();
    transfer.Add(item);
    transfer.Add(DataTransferItem.CreateText("Second"));
    await clipboard.SetDataAsync(transfer);
    await clipboard.FlushAsync();
    Check(await clipboard.TryGetInProcessDataAsync() is null, "The system provider does not keep in-process data.");

    using (var pasted = await clipboard.TryGetDataAsync())
    {
        Check(pasted is { Items.Count: 2 }, "Record count");
        var first = pasted!.Items[0];
        Check(await first.TryGetTextAsync() == "First — 中文 🎧" &&
            await first.TryGetValueAsync(html) == "<b>First</b>" &&
            await pasted.Items[1].TryGetTextAsync() == "Second", "Record order and coexisting text and HTML");
        Check(!first.Contains(inProcess) && await first.TryGetValueAsync(customText) == "α\0🎧" &&
            await first.TryGetValueAsync(customBytes) is { Length: 0 }, "Typed application formats");
        using var bitmap = await first.TryGetBitmapAsync();
        Check(bitmap?.PixelSize == bitmapSize && ReadBitmap(bitmap).AsSpan().SequenceEqual(expectedPixels),
            "Opaque, translucent, and transparent bitmap pixels");
    }

    nativeMode(1);
    using (var malformed = await clipboard.TryGetDataAsync())
        Check(malformed is not null && !malformed.Items[0].Contains(DataFormat.Bitmap) &&
            await malformed.Items[0].TryGetTextAsync() == "First — 中文 🎧", "A missing native PixelMap preserves other formats");

    nativeMode(2);
    try
    {
        using var denied = await clipboard.TryGetDataAsync();
        throw new InvalidOperationException("A denied clipboard read succeeded.");
    }
    catch (UnauthorizedAccessException) { }

    nativeMode(0);
    try
    {
        await clipboard.SetValueAsync(customBytes, new byte[4096]);
        throw new InvalidOperationException("An oversized custom value succeeded.");
    }
    catch (ArgumentOutOfRangeException) { }
    Check(await clipboard.TryGetTextAsync() == "First — 中文 🎧", "A rejected write preserves existing data");
    await clipboard.ClearAsync();
    Check(await clipboard.TryGetDataAsync() is null, "Empty clipboard after clear");
});
Console.WriteLine("Clipboard text, HTML, bitmap, application formats, denied read and clear passed.");

static unsafe Bitmap MakeBitmap(byte[] pixels, PixelSize size)
{
    fixed (byte* data = pixels)
        return new Bitmap(PixelFormats.Rgba8888, AlphaFormat.Premul, (nint)data, size, new Vector(96, 96), size.Width * 4);
}

static unsafe byte[] ReadBitmap(Bitmap bitmap)
{
    var size = bitmap.PixelSize;
    var pixels = new byte[size.Width * size.Height * 4];
    fixed (byte* data = pixels)
    {
        using var framebuffer = new LockedFramebuffer((nint)data, size, size.Width * 4, bitmap.Dpi,
            PixelFormats.Rgba8888, AlphaFormat.Premul, null);
        bitmap.CopyPixels(framebuffer);
    }
    return pixels;
}

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

delegate void SetProbeMode(int mode);

sealed class ClipboardApp : Application
{
    internal static TextBox TextBox { get; } = new();

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime lifetime)
            lifetime.MainView = TextBox;
        base.OnFrameworkInitializationCompleted();
    }
}
