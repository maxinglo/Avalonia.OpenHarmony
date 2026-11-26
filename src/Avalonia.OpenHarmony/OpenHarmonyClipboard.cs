using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using static Avalonia.OpenHarmony.PasteboardApi;

namespace Avalonia.OpenHarmony;

internal sealed class OpenHarmonyClipboard : IClipboard
{
    private const int PermissionDenied = 201;
    private const int InvalidParameter = 401;
    private const int ReadFailed = 12900010;
    private const int MaximumBitmapPixels = 16_777_216;
    private const int MaximumApplicationEntrySize = 4096;
    private const byte BytesEntry = 1;
    private const byte Utf8Entry = 2;
    private const string PlainTextType = "general.plain-text";
    private const string HtmlType = "general.html";
    private const string PixelMapType = "openharmony.pixel-map";
    private const string ApplicationTypePrefix = "ApplicationDefined.Avalonia.";

    private static readonly DataFormat<string> HtmlFormat = DataFormat.CreateStringPlatformFormat("text/html");
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly SemaphoreSlim NativeOperations = new(1, 1);

    internal static OpenHarmonyClipboard Instance { get; } = new();

    public async Task SetDataAsync(IAsyncDataTransfer? dataTransfer)
    {
        if (dataTransfer is null)
        {
            await ClearAsync().ConfigureAwait(false);
            return;
        }

        var items = new List<ClipboardItem>();
        using (dataTransfer)
        {
            if (dataTransfer.Items.Count == 0)
            {
                await ClearAsync().ConfigureAwait(false);
                return;
            }

            foreach (var item in dataTransfer.Items)
            {
                // Keep data providers on the caller's context, since they can depend on UI state.
                var text = item.Contains(DataFormat.Text) ? await item.TryGetTextAsync() : null;
                var html = item.Contains(HtmlFormat) ? await item.TryGetValueAsync(HtmlFormat) : null;
                // The UDS text setters accept NUL-terminated UTF-8 strings.
                if (text?.Contains('\0') == true || html?.Contains('\0') == true)
                    throw new COMException("Clipboard text cannot contain U+0000 on OpenHarmony.", InvalidParameter);

                var bitmap = item.Contains(DataFormat.Bitmap) ? await item.TryGetBitmapAsync() : null;
                var pixels = bitmap is null ? null : CopyBitmap(bitmap);
                List<ApplicationEntry>? applicationEntries = null;
                foreach (var format in item.Formats)
                {
                    if (format.Kind != DataFormatKind.Application)
                        continue;

                    var value = await item.TryGetRawAsync(format);
                    var entry = value switch
                    {
                        byte[] bytes => EncodeApplicationEntry(format, bytes),
                        string content => EncodeApplicationEntry(format, content),
                        _ => null
                    };
                    if (entry is not null)
                        (applicationEntries ??= []).Add(entry);
                }

                if (text is not null || html is not null || pixels is not null || applicationEntries is not null)
                    items.Add(new ClipboardItem(text, html, pixels, applicationEntries));
            }
        }

        if (items.Count == 0)
            throw new NotSupportedException("The OpenHarmony clipboard supports text, HTML, bitmaps and application formats.");

        await RunOnWorkerAsync(() => Write(items)).ConfigureAwait(false);
    }

    public Task ClearAsync() => RunOnWorkerAsync(() =>
    {
        using var pasteboard = new NativeAllocation(OH_Pasteboard_Create(), OH_Pasteboard_Destroy, "pasteboard");
        Check(OH_Pasteboard_ClearData(pasteboard.Value), "clear");
    });

    public Task<IAsyncDataTransfer?> TryGetDataAsync() => RunOnWorkerAsync(Read);

    public Task FlushAsync() => Task.CompletedTask;

    public Task<IAsyncDataTransfer?> TryGetInProcessDataAsync() => Task.FromResult<IAsyncDataTransfer?>(null);

    private static unsafe void Write(List<ClipboardItem> items)
    {
        using var pasteboard = new NativeAllocation(OH_Pasteboard_Create(), OH_Pasteboard_Destroy, "pasteboard");
        using var data = new NativeAllocation(OH_UdmfData_Create(), OH_UdmfData_Destroy, "clipboard data");
        foreach (var item in items)
        {
            using var record = new NativeAllocation(OH_UdmfRecord_Create(), OH_UdmfRecord_Destroy, "clipboard record");
            if (item.Text is not null)
            {
                using var text = new NativeAllocation(OH_UdsPlainText_Create(), OH_UdsPlainText_Destroy, "plain text");
                Check(OH_UdsPlainText_SetContent(text.Value, item.Text), "encode plain text");
                Check(OH_UdmfRecord_AddPlainText(record.Value, text.Value), "add plain text");
            }
            if (item.Html is not null)
            {
                using var html = new NativeAllocation(OH_UdsHtml_Create(), OH_UdsHtml_Destroy, "HTML");
                Check(OH_UdsHtml_SetContent(html.Value, item.Html), "encode HTML");
                if (item.Text is not null)
                    Check(OH_UdsHtml_SetPlainContent(html.Value, item.Text), "encode HTML fallback text");
                Check(OH_UdmfRecord_AddHtml(record.Value, html.Value), "add HTML");
            }
            if (item.Bitmap is { } bitmap)
                WriteBitmap(record.Value, bitmap);
            if (item.ApplicationEntries is { } entries)
            {
                foreach (var entry in entries)
                {
                    fixed (byte* bytes = entry.Bytes)
                        Check(OH_UdmfRecord_AddGeneralEntry(record.Value, entry.Type, bytes, (uint)entry.Bytes.Length),
                            "add application entry");
                }
            }
            Check(OH_UdmfData_AddRecord(data.Value, record.Value), "add clipboard record");
        }
        Check(OH_Pasteboard_SetData(pasteboard.Value, data.Value), "write");
    }

    private static unsafe IAsyncDataTransfer? Read()
    {
        using var pasteboard = new NativeAllocation(OH_Pasteboard_Create(), OH_Pasteboard_Destroy, "pasteboard");
        if (!OH_Pasteboard_HasData(pasteboard.Value))
            return null;

        var data = OH_Pasteboard_GetData(pasteboard.Value, out var status);
        try
        {
            // Another process can clear the clipboard between the metadata and data requests.
            if (data == 0 && (status == 0 || status == ReadFailed && !OH_Pasteboard_HasData(pasteboard.Value)))
                return null;
            Check(status, "read");

            var records = OH_UdmfData_GetRecords(data, out var count);
            if (records == null)
            {
                if (count == 0)
                    return null;
                throw new COMException("OpenHarmony returned missing clipboard records.");
            }

            // Both the record array and individual records are owned by the UDMF data.
            var result = new DataTransfer();
            foreach (var record in new ReadOnlySpan<nint>(records, checked((int)count)))
            {
                if (record != 0 && ReadRecord(record) is { } item)
                    result.Add(item);
            }
            return result.Items.Count == 0 ? null : result;
        }
        finally
        {
            if (data != 0)
                OH_UdmfData_Destroy(data);
        }
    }

    private static unsafe DataTransferItem? ReadRecord(nint record)
    {
        var types = OH_UdmfRecord_GetTypes(record, out var count);
        if (types == null)
        {
            if (count == 0)
                return null;
            throw new COMException("OpenHarmony returned missing clipboard record types.");
        }

        var hasText = false;
        var hasHtml = false;
        var hasBitmap = false;
        List<string>? applicationTypes = null;
        foreach (var type in new ReadOnlySpan<nint>(types, checked((int)count)))
        {
            var name = Marshal.PtrToStringUTF8(type);
            hasText |= name == PlainTextType;
            hasHtml |= name == HtmlType;
            hasBitmap |= name == PixelMapType;
            if (name?.StartsWith(ApplicationTypePrefix, StringComparison.Ordinal) == true)
                (applicationTypes ??= []).Add(name);
        }
        if (!hasText && !hasHtml && !hasBitmap && applicationTypes is null)
            return null;

        var item = new DataTransferItem();
        if (hasText)
        {
            using var text = new NativeAllocation(OH_UdsPlainText_Create(), OH_UdsPlainText_Destroy, "plain text");
            Check(OH_UdmfRecord_GetPlainText(record, text.Value), "decode plain text");
            item.Set(DataFormat.Text, Marshal.PtrToStringUTF8(OH_UdsPlainText_GetContent(text.Value)) ?? string.Empty);
        }
        if (hasHtml)
        {
            using var html = new NativeAllocation(OH_UdsHtml_Create(), OH_UdsHtml_Destroy, "HTML");
            Check(OH_UdmfRecord_GetHtml(record, html.Value), "decode HTML");
            item.Set(HtmlFormat, Marshal.PtrToStringUTF8(OH_UdsHtml_GetContent(html.Value)) ?? string.Empty);
            if (!hasText && Marshal.PtrToStringUTF8(OH_UdsHtml_GetPlainContent(html.Value)) is { Length: > 0 } fallback)
                item.Set(DataFormat.Text, fallback);
        }
        if (hasBitmap && TryReadBitmap(record) is { } bitmap)
            item.Set(DataFormat.Bitmap, bitmap.CreateBitmap);
        if (applicationTypes is not null)
        {
            foreach (var type in applicationTypes)
                ReadApplicationEntry(record, type, item);
        }
        return item.Formats.Count == 0 ? null : item;
    }

    private static unsafe BitmapPixels CopyBitmap(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        if (size.Width <= 0 || size.Height <= 0 || (long)size.Width * size.Height > MaximumBitmapPixels)
            throw new ArgumentOutOfRangeException(nameof(bitmap), "The clipboard bitmap exceeds the supported pixel count.");

        var stride = size.Width * 4;
        var pixels = new byte[stride * size.Height];
        fixed (byte* address = pixels)
        {
            // The PixelMap constructor takes straight-alpha input, then converts to the requested alpha type.
            using var target = new LockedFramebuffer((nint)address, size, stride, bitmap.Dpi,
                PixelFormats.Rgba8888, AlphaFormat.Unpremul, null);
            bitmap.CopyPixels(target);
        }
        return new BitmapPixels(size, pixels);
    }

    private static unsafe void WriteBitmap(nint record, BitmapPixels bitmap)
    {
        using var options = CreatePixelMapOptions((uint)bitmap.Size.Width, (uint)bitmap.Size.Height);
        nint pixelMap;
        fixed (byte* pixels = bitmap.Bytes)
            Check(OH_PixelmapNative_CreatePixelmap(pixels, (nuint)bitmap.Bytes.Length, options.Value, out pixelMap),
                "create PixelMap");
        using var image = new NativeAllocation(pixelMap, DestroyPixelMap, "PixelMap");
        using var uds = new NativeAllocation(OH_UdsPixelMap_Create(), OH_UdsPixelMap_Destroy, "UDS PixelMap");
        Check(OH_UdsPixelMap_SetPixelMap(uds.Value, image.Value), "encode bitmap");
        Check(OH_UdmfRecord_AddPixelMap(record, uds.Value), "add bitmap");
    }

    private static unsafe BitmapPixels? TryReadBitmap(nint record)
    {
        using var uds = new NativeAllocation(OH_UdsPixelMap_Create(), OH_UdsPixelMap_Destroy, "UDS PixelMap");
        if (OH_UdmfRecord_GetPixelMap(record, uds.Value) != 0)
            return null;

        // The UDS getter fills an existing PixelMap. Its void return leaves the placeholder intact on failure.
        using var options = CreatePixelMapOptions(1, 1);
        Check(OH_PixelmapNative_CreateEmptyPixelmap(options.Value, out var pixelMap), "create empty PixelMap");
        using var image = new NativeAllocation(pixelMap, DestroyPixelMap, "PixelMap");
        Check(OH_PixelmapNative_GetUniqueId(image.Value, out var placeholder), "identify empty PixelMap");
        OH_UdsPixelMap_GetPixelMap(uds.Value, image.Value);
        if (OH_PixelmapNative_GetUniqueId(image.Value, out var imageId) != 0 || imageId == placeholder)
            return null;

        Check(OH_PixelmapImageInfo_Create(out var imageInfo), "create PixelMap info");
        using var info = new NativeAllocation(imageInfo, static value => OH_PixelmapImageInfo_Release(value), "PixelMap info");
        if (OH_PixelmapNative_GetImageInfo(image.Value, info.Value) != 0 ||
            OH_PixelmapImageInfo_GetWidth(info.Value, out var width) != 0 ||
            OH_PixelmapImageInfo_GetHeight(info.Value, out var height) != 0 ||
            width == 0 || height == 0 || (ulong)width * height > MaximumBitmapPixels)
            return null;

        var pixels = new byte[(int)(width * height * 4)];
        fixed (byte* address = pixels)
        {
            // This API converts the selected area into tightly packed BGRA with straight alpha.
            var area = new PixelArea(address, width, height);
            if (OH_PixelmapNative_ReadPixelsFromArea(image.Value, in area) != 0)
                return null;
        }
        return new BitmapPixels(new PixelSize((int)width, (int)height), pixels);
    }

    private static NativeAllocation CreatePixelMapOptions(uint width, uint height)
    {
        Check(OH_PixelmapInitializationOptions_Create(out var address), "create PixelMap options");
        var options = new NativeAllocation(address, static value => OH_PixelmapInitializationOptions_Release(value),
            "PixelMap options");
        try
        {
            Check(OH_PixelmapInitializationOptions_SetWidth(address, width), "set PixelMap width");
            Check(OH_PixelmapInitializationOptions_SetHeight(address, height), "set PixelMap height");
            Check(OH_PixelmapInitializationOptions_SetPixelFormat(address, Rgba8888), "set PixelMap format");
            Check(OH_PixelmapInitializationOptions_SetSrcPixelFormat(address, Rgba8888), "set PixelMap source format");
            Check(OH_PixelmapInitializationOptions_SetAlphaType(address, PremultipliedAlpha), "set PixelMap alpha");
            return options;
        }
        catch
        {
            options.Dispose();
            throw;
        }
    }

    private static void DestroyPixelMap(nint value) => OH_PixelmapNative_Destroy(ref value);

    private static ApplicationEntry EncodeApplicationEntry(DataFormat format, byte[] value)
    {
        var entry = CreateApplicationEntry(format, BytesEntry, value.Length);
        value.CopyTo(entry.Bytes.AsSpan(1));
        return entry;
    }

    private static ApplicationEntry EncodeApplicationEntry(DataFormat format, string value)
    {
        var entry = CreateApplicationEntry(format, Utf8Entry, StrictUtf8.GetByteCount(value));
        StrictUtf8.GetBytes(value, entry.Bytes.AsSpan(1));
        return entry;
    }

    private static ApplicationEntry CreateApplicationEntry(DataFormat format, byte kind, int payloadLength)
    {
        // The one-byte type marker lets an application format round-trip as bytes or UTF-8, including empty values.
        if (payloadLength >= MaximumApplicationEntrySize)
            throw new ArgumentOutOfRangeException(nameof(payloadLength), "The application clipboard value must fit in 4095 bytes.");
        var data = new byte[payloadLength + 1];
        data[0] = kind;
        return new ApplicationEntry(format.ToSystemName(ApplicationTypePrefix), data);
    }

    private static unsafe void ReadApplicationEntry(nint record, string type, DataTransferItem item)
    {
        // The result buffer belongs to the record and can be invalidated by the next entry read.
        if (OH_UdmfRecord_GetGeneralEntry(record, type, out var address, out var size) != 0 ||
            address == null || size is 0 or > MaximumApplicationEntrySize)
            return;

        var payload = new ReadOnlySpan<byte>(address + 1, (int)size - 1);
        if (address[0] == BytesEntry &&
            DataFormat.FromSystemName<byte[]>(type, ApplicationTypePrefix) is { Kind: DataFormatKind.Application } bytes)
            item.Set(bytes, payload.ToArray());
        if (address[0] == Utf8Entry &&
            DataFormat.FromSystemName<string>(type, ApplicationTypePrefix) is { Kind: DataFormatKind.Application } text)
        {
            try { item.Set(text, StrictUtf8.GetString(payload)); }
            catch (DecoderFallbackException) { }
        }
    }

    private static Task RunOnWorkerAsync(Action operation) =>
        RunOnWorkerAsync(() =>
        {
            operation();
            return true;
        });

    private static async Task<T> RunOnWorkerAsync<T>(Func<T> operation)
    {
        await NativeOperations.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(operation).ConfigureAwait(false);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new COMException("The OpenHarmony clipboard libraries are unavailable.", error);
        }
        finally
        {
            NativeOperations.Release();
        }
    }

    private static void Check(int status, string operation)
    {
        if (status == PermissionDenied)
            throw new UnauthorizedAccessException(
                $"OpenHarmony denied clipboard {operation} (201). Reading requires an ArkUI PasteButton or an approved READ_PASTEBOARD grant.");
        if (status != 0)
            throw new COMException($"OpenHarmony clipboard {operation} failed ({status}).", status);
    }

    private sealed record ClipboardItem(string? Text, string? Html, BitmapPixels? Bitmap,
        List<ApplicationEntry>? ApplicationEntries);

    private sealed record ApplicationEntry(string Type, byte[] Bytes);

    private sealed record BitmapPixels(PixelSize Size, byte[] Bytes)
    {
        internal unsafe Bitmap CreateBitmap()
        {
            fixed (byte* pixels = Bytes)
                return new Bitmap(PixelFormats.Bgra8888, AlphaFormat.Unpremul, (nint)pixels, Size, new Vector(96, 96), Size.Width * 4);
        }
    }

    private readonly struct NativeAllocation(nint address, Action<nint> destroy, string description) : IDisposable
    {
        internal nint Value { get; } = address != 0
            ? address
            : throw new COMException($"Could not allocate OpenHarmony {description}.");

        public void Dispose() => destroy(Value);
    }
}
