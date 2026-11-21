using System.Text.Json;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Avalonia.OpenHarmony;

public sealed partial class OpenHarmonyHost
{
    private (long Token, SurfaceTopLevel? Surface) _inboundDrag;

    /// <summary>Delivers system drag events and returns the Avalonia destination's acceptance of a Copy.</summary>
    public bool ReceiveDrag(long surface, int phase, double physicalX, double physicalY, int modifiers, int formats,
        string payload)
    {
        if (surface <= 0 || phase is < 0 or > 3 || !double.IsFinite(physicalX) || !double.IsFinite(physicalY) ||
            payload.Length > 1_048_576)
            return false;

        Dispatcher? dispatcher;
        lock (_dispatchLock)
            dispatcher = _stopped ? null : _dispatcher;
        if (dispatcher is null)
            return false;

        var type = (RawDragEventType)phase;
        if (type == RawDragEventType.DragLeave)
        {
            if (dispatcher.CheckAccess())
                CancelInboundDrag(surface);
            else
                Post(() => CancelInboundDrag(surface));
            return false;
        }

        bool DispatchDrag() => DispatchInboundDrag(surface, type, physicalX, physicalY,
            (RawInputModifiers)modifiers & RawInputModifiers.KeyboardMask, formats, payload);

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
            return false;
        }
        catch (Exception error)
        {
            _reportError(error.ToString());
            return false;
        }
    }

    private bool DispatchInboundDrag(long token, RawDragEventType type, double x, double y,
        RawInputModifiers modifiers, int formats, string payload)
    {
        if (FindDragReceiver(token) is not { } destination)
        {
            CancelInboundDrag(token);
            return false;
        }

        if (_inboundDrag.Token != token || type == RawDragEventType.DragEnter)
            CancelInboundDrag(_inboundDrag.Token);
        _inboundDrag = (token, destination);

        try
        {
            return destination.ReceiveDrag(type, x, y, modifiers, formats, payload);
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
    private const int MaximumDropItems = 32;
    private readonly DragDropDevice _dragDevice = new();
    private Point _lastDragPosition;
    private RawInputModifiers _lastDragModifiers;
    private bool _dragInProgress;

    internal bool ReceiveDrag(RawDragEventType type, double x, double y, RawInputModifiers modifiers, int formats,
        string payload)
    {
        if (_inputRoot is not { } root || Input is not { } process)
            return false;

        var position = new Point(x / RenderScaling, y / RenderScaling);
        _lastDragPosition = position;
        _lastDragModifiers = modifiers;
        _dragInProgress = true;
        var storage = _storageProvider as OpenHarmonyStorageProvider;
        if (type != RawDragEventType.Drop)
        {
            using var offered = new OfferedTransfer(formats, storage is not null);
            var proposed = offered.Formats.Count == 0 ? DragDropEffects.None : DragDropEffects.Copy;
            return Dispatch(type, offered, proposed) && proposed == DragDropEffects.Copy;
        }

        using var received = new DataTransfer();
        var ownedFiles = new List<IStorageFile>();
        var accepted = false;
        try
        {
            if (!ReadPayload(payload, storage, received, ownedFiles) || received.Items.Count == 0)
                return false;

            var actualFormats = (received.Formats.Contains(DataFormat.Text) ? PlainTextOffer : 0) |
                (received.Formats.Contains(DataFormat.File) ? FileOffer : 0);
            using var offered = new OfferedTransfer(actualFormats, storage is not null);
            // Some systems dispatch Drop without a final motion event. Keep payloads private until Drop.
            if (!Dispatch(RawDragEventType.DragOver, offered, DragDropEffects.Copy))
                return false;

            _dragInProgress = false;
            accepted = Dispatch(RawDragEventType.Drop, received, DragDropEffects.Copy);
            return accepted;
        }
        finally
        {
            if (!accepted)
                foreach (var file in ownedFiles)
                    file.Dispose();
        }

        bool Dispatch(RawDragEventType eventType, IDataTransfer transfer, DragDropEffects proposed)
        {
            var drag = new RawDragEvent(_dragDevice, eventType, root, position, transfer, proposed, modifiers);
            process(drag);
            return (drag.Effects & DragDropEffects.Copy) == DragDropEffects.Copy;
        }
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
            if (data.ValueKind != JsonValueKind.Object ||
                !data.TryGetProperty("texts", out var texts) || texts.ValueKind != JsonValueKind.Array ||
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

    private sealed class OfferedTransfer : IDataTransfer, IDataTransferItem
    {
        public OfferedTransfer(int formats, bool supportsFiles)
        {
            var offered = new List<DataFormat>(2);
            if ((formats & PlainTextOffer) != 0)
                offered.Add(DataFormat.Text);
            if (supportsFiles && (formats & FileOffer) != 0)
                offered.Add(DataFormat.File);
            Formats = offered;
            Items = offered.Count == 0 ? [] : [this];
        }

        public IReadOnlyList<DataFormat> Formats { get; }
        public IReadOnlyList<IDataTransferItem> Items { get; }
        public object? TryGetRaw(DataFormat format) => null;
        public void Dispose() { }
    }
}
