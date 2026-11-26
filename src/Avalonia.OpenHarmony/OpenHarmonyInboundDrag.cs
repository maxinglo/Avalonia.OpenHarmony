using System.Text.Json;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Avalonia.OpenHarmony;

public sealed partial class OpenHarmonyHost
{
    private const int AllowTextMove = 16;
    private (long Token, SurfaceTopLevel? Surface) _inboundDrag;

    /// <summary>Delivers system drag events and returns the Avalonia destination's acceptance of a Copy.</summary>
    public bool ReceiveDrag(long surface, int phase, double physicalX, double physicalY, int modifiers, int formats,
        string payload) => ReceiveDragEffect(surface, phase, physicalX, physicalY, modifiers, formats & ~AllowTextMove,
        payload) == DragDropEffects.Copy;

    /// <summary>Delivers system drag events and returns the Avalonia destination's chosen drop effect.</summary>
    public DragDropEffects ReceiveDragEffect(long surface, int phase, double physicalX, double physicalY,
        int modifiers, int formats, string payload)
    {
        if (surface <= 0 || phase is < 0 or > 3 || !double.IsFinite(physicalX) || !double.IsFinite(physicalY) ||
            payload.Length > 1_048_576)
            return DragDropEffects.None;

        Dispatcher? dispatcher;
        lock (_dispatchLock)
            dispatcher = _stopped ? null : _dispatcher;
        if (dispatcher is null)
            return DragDropEffects.None;

        var type = (RawDragEventType)phase;
        if (type == RawDragEventType.DragLeave)
        {
            if (dispatcher.CheckAccess())
                CancelInboundDrag(surface);
            else
                Post(() => CancelInboundDrag(surface));
            return DragDropEffects.None;
        }

        DragDropEffects DispatchDrag() => DispatchInboundDrag(surface, type, physicalX, physicalY,
            (RawInputModifiers)modifiers & RawInputModifiers.KeyboardMask, formats & ~AllowTextMove, payload,
            (formats & AllowTextMove) != 0);

        try
        {
            if (dispatcher.CheckAccess())
                return DispatchDrag();

            // ArkUI needs the result inside its callback. Cancel work that has not started rather than delivering
            // a drop after the OS-side callback already rejected it and closed its borrowed descriptors.
            using var queueTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            return dispatcher.InvokeAsync(DispatchDrag, DispatcherPriority.Input, queueTimeout.Token)
                .GetTask().GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            return DragDropEffects.None;
        }
        catch (Exception error)
        {
            _reportError(error.ToString());
            return DragDropEffects.None;
        }
    }

    private DragDropEffects DispatchInboundDrag(long token, RawDragEventType type, double x, double y,
        RawInputModifiers modifiers, int formats, string payload, bool allowTextMove)
    {
        if (FindDragReceiver(token) is not { } destination)
        {
            CancelInboundDrag(token);
            return DragDropEffects.None;
        }

        if (_inboundDrag.Token != token || type == RawDragEventType.DragEnter)
            CancelInboundDrag(_inboundDrag.Token);
        _inboundDrag = (token, destination);

        try
        {
            return destination.ReceiveDrag(type, x, y, modifiers, formats, payload, allowTextMove);
        }
        catch
        {
            CancelInboundDrag(token);
            throw;
        }
        finally
        {
            if (type == RawDragEventType.Drop)
                CancelInboundDrag(token);
        }
    }

    private SurfaceTopLevel? FindDragReceiver(long token) =>
        _applicationForeground && (_stageRoute is not null || _windowVisible) ? FindInputSurface(token) : null;

    private void CancelUnavailableInboundDrag()
    {
        if (_inboundDrag.Surface is not null && FindDragReceiver(_inboundDrag.Token) is null)
            CancelInboundDrag(_inboundDrag.Token);
    }

    private void CancelInboundDrag(long token)
    {
        if (_inboundDrag.Token != token || _inboundDrag.Surface is not { } destination)
            return;
        _inboundDrag = default;
        destination.CancelInboundDrag();
    }
}

internal sealed partial class SurfaceTopLevel
{
    private const int PlainTextOffer = 1;
    private const int FileOffer = 2;
    private const int HtmlOffer = 4;
    private const int HyperlinkOffer = 8;
    private const int MaximumDropItems = 32;
    private readonly DragDropDevice _dragDevice = new();
    private Point _lastDragPosition;
    private RawInputModifiers _lastDragModifiers;
    private bool _dragInProgress;

    internal DragDropEffects ReceiveDrag(RawDragEventType type, double x, double y, RawInputModifiers modifiers,
        int formats, string payload, bool allowTextMove)
    {
        if (_inputRoot is not { } root || Input is not { } process)
            return DragDropEffects.None;

        var position = new Point(x / RenderScaling, y / RenderScaling);
        _lastDragPosition = position;
        _lastDragModifiers = modifiers;
        _dragInProgress = true;
        var storage = _storageProvider as OpenHarmonyStorageProvider;
        if (type != RawDragEventType.Drop)
        {
            using var offered = new OfferedTransfer(formats, storage is not null);
            return Dispatch(type, offered, Propose(formats, offered.Formats.Count > 0, allowTextMove));
        }

        using var received = new DataTransfer();
        var ownedFiles = new List<IStorageFile>();
        var accepted = false;
        try
        {
            if (!ReadPayload(payload, storage, received, ownedFiles) || received.Items.Count == 0)
                return DragDropEffects.None;

            var actualFormats = (received.Formats.Contains(DataFormat.Text) ? PlainTextOffer : 0) |
                (received.Formats.Contains(DataFormat.File) ? FileOffer : 0) |
                (received.Formats.Contains(OpenHarmonyDragFormats.Html) ? HtmlOffer : 0) |
                (received.Formats.Contains(OpenHarmonyDragFormats.UriList) ? HyperlinkOffer : 0);
            using var offered = new OfferedTransfer(actualFormats, storage is not null);
            var proposed = Propose(actualFormats | (formats & FileOffer), true, allowTextMove);
            // Some systems dispatch Drop without a final motion event. Keep payloads private until Drop.
            if (Dispatch(RawDragEventType.DragOver, offered, proposed) == DragDropEffects.None)
                return DragDropEffects.None;

            _dragInProgress = false;
            var effect = Dispatch(RawDragEventType.Drop, received, proposed);
            accepted = effect != DragDropEffects.None;
            return effect;
        }
        finally
        {
            if (!accepted)
                foreach (var file in ownedFiles)
                    file.Dispose();
        }

        DragDropEffects Dispatch(RawDragEventType eventType, IDataTransfer transfer, DragDropEffects proposed)
        {
            var drag = new RawDragEvent(_dragDevice, eventType, root, position, transfer, proposed, modifiers);
            process(drag);
            var requested = drag.Effects & proposed;
            return (requested & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : requested & DragDropEffects.Move;
        }
    }

    private static DragDropEffects Propose(int formats, bool hasData, bool allowTextMove)
    {
        if (!hasData)
            return DragDropEffects.None;
        return allowTextMove && (formats & FileOffer) == 0
            ? DragDropEffects.Copy | DragDropEffects.Move
            : DragDropEffects.Copy;
    }

    internal void CancelInboundDrag()
    {
        if (!_dragInProgress)
            return;
        _dragInProgress = false;

        if (_inputRoot is not { } root || Input is not { } process)
            return;
        using var empty = new DataTransfer();
        try
        {
            process(new RawDragEvent(_dragDevice, RawDragEventType.DragLeave, root,
                _lastDragPosition, empty, DragDropEffects.None, _lastDragModifiers));
        }
        catch (Exception error)
        {
            _reportError(error.ToString());
        }
    }

    private static bool ReadPayload(string payload, OpenHarmonyStorageProvider? storage, DataTransfer transfer,
        List<IStorageFile> ownedFiles)
    {
        if (payload.Length == 0)
            return false;
        try
        {
            using var document = JsonDocument.Parse(payload);
            var data = document.RootElement;
            if (data.ValueKind != JsonValueKind.Object)
                return false;
            if (data.TryGetProperty("items", out var items))
            {
                if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > MaximumDropItems)
                    return false;
                foreach (var record in items.EnumerateArray())
                    AddItem(record, storage, transfer, ownedFiles);
                return true;
            }

            if (!data.TryGetProperty("texts", out var texts) || texts.ValueKind != JsonValueKind.Array ||
                !data.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array ||
                texts.GetArrayLength() + files.GetArrayLength() > MaximumDropItems)
                return false;

            foreach (var text in texts.EnumerateArray())
                if (text.ValueKind == JsonValueKind.String)
                    transfer.Add(DataTransferItem.CreateText(text.GetString()));

            if (storage is null)
                return true;

            foreach (var file in files.EnumerateArray())
            {
                if (file.ValueKind != JsonValueKind.Object ||
                    !file.TryGetProperty("uri", out var uri) || uri.ValueKind != JsonValueKind.String ||
                    !file.TryGetProperty("descriptor", out var descriptor) || descriptor.ValueKind != JsonValueKind.Number ||
                    !descriptor.TryGetInt32(out var fd) ||
                    !file.TryGetProperty("length", out var length) || length.ValueKind != JsonValueKind.Number ||
                    !length.TryGetUInt64(out var size) ||
                    storage.RetainDroppedFile(uri.GetString()!, fd, size) is not { } item)
                    continue;

                ownedFiles.Add(item);
                transfer.Add(DataTransferItem.CreateFile(item));
            }
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void AddItem(JsonElement record, OpenHarmonyStorageProvider? storage, DataTransfer transfer,
        List<IStorageFile> ownedFiles)
    {
        if (record.ValueKind != JsonValueKind.Object)
            return;

        var item = new DataTransferItem();
        if (record.TryGetProperty("file", out var file))
        {
            if (storage is null || file.ValueKind != JsonValueKind.Object || ReadString(file, "uri") is not { } uri ||
                !file.TryGetProperty("descriptor", out var descriptor) || descriptor.ValueKind != JsonValueKind.Number ||
                !descriptor.TryGetInt32(out var fd) ||
                !file.TryGetProperty("length", out var length) || length.ValueKind != JsonValueKind.Number ||
                !length.TryGetUInt64(out var size))
                return;

            var writable = file.TryGetProperty("writable", out var access) && access.ValueKind == JsonValueKind.True;
            if (storage.RetainDroppedFile(uri, fd, size, writable) is not { } retained)
                return;
            ownedFiles.Add(retained);
            item.Set(DataFormat.File, retained);
        }
        if (ReadString(record, "text") is { } text)
            item.Set(DataFormat.Text, text);
        if (ReadString(record, "html") is { } html)
            item.Set(OpenHarmonyDragFormats.Html, html);
        if (ReadString(record, "link") is { } link && OpenHarmonyDragFormats.IsWebLink(link))
            item.Set(OpenHarmonyDragFormats.UriList, link);
        if (item.Formats.Count > 0)
            transfer.Add(item);
    }

    private static string? ReadString(JsonElement record, string key) =>
        record.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private sealed class OfferedTransfer : IDataTransfer, IDataTransferItem
    {
        public OfferedTransfer(int formats, bool supportsFiles)
        {
            var offered = new List<DataFormat>(4);
            if ((formats & PlainTextOffer) != 0)
                offered.Add(DataFormat.Text);
            if (supportsFiles && (formats & FileOffer) != 0)
                offered.Add(DataFormat.File);
            if ((formats & HtmlOffer) != 0)
                offered.Add(OpenHarmonyDragFormats.Html);
            if ((formats & HyperlinkOffer) != 0)
                offered.Add(OpenHarmonyDragFormats.UriList);
            Formats = offered;
            Items = offered.Count == 0 ? [] : [this];
        }

        public IReadOnlyList<DataFormat> Formats { get; }
        public IReadOnlyList<IDataTransferItem> Items { get; }
        public object? TryGetRaw(DataFormat format) => null;
        public void Dispose() { }
    }
}
