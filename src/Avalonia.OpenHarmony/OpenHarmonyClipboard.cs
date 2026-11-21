using System.Runtime.InteropServices;
using Avalonia.Input;
using Avalonia.Input.Platform;
using static Avalonia.OpenHarmony.PasteboardApi;

namespace Avalonia.OpenHarmony;

internal sealed class OpenHarmonyClipboard : IClipboard
{
    private const int PermissionDenied = 201;
    private const int InvalidParameter = 401;
    private const int ReadFailed = 12900010;
    private const string PlainTextType = "general.plain-text";
    private const string HtmlType = "general.html";

    private static readonly DataFormat<string> HtmlFormat = DataFormat.CreateStringPlatformFormat("text/html");
    private static readonly SemaphoreSlim NativeOperations = new(1, 1);

    internal static OpenHarmonyClipboard Instance { get; } = new();

    public async Task SetDataAsync(IAsyncDataTransfer? dataTransfer)
    {
        if (dataTransfer is null)
        {
            await ClearAsync().ConfigureAwait(false);
            return;
        }

        var items = new List<TextItem>();
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
                if (text is not null || html is not null)
                    items.Add(new TextItem(text, html));
            }
        }

        if (items.Count == 0)
            throw new NotSupportedException("The OpenHarmony clipboard supports plain text and text/html.");

        foreach (var item in items)
        {
            // The UDS text setters accept NUL-terminated UTF-8 strings.
            if (item.Text?.Contains('\0') == true || item.Html?.Contains('\0') == true)
                throw new COMException("Clipboard text cannot contain U+0000 on OpenHarmony.", InvalidParameter);
        }

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

    private static void Write(List<TextItem> items)
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
        foreach (var type in new ReadOnlySpan<nint>(types, checked((int)count)))
        {
            var name = Marshal.PtrToStringUTF8(type);
            hasText |= name == PlainTextType;
            hasHtml |= name == HtmlType;
        }
        if (!hasText && !hasHtml)
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
        return item;
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

    private readonly record struct TextItem(string? Text, string? Html);

    private readonly struct NativeAllocation(nint address, Action<nint> destroy, string description) : IDisposable
    {
        internal nint Value { get; } = address != 0
            ? address
            : throw new COMException($"Could not allocate OpenHarmony {description}.");

        public void Dispose() => destroy(Value);
    }
}
